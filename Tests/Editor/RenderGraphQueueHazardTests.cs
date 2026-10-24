using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using InfinityTech.Rendering.GPUResource;
using InfinityTech.Rendering.RenderGraph;

namespace InfinityTech.Rendering.Pipeline.Tests
{
    public class RenderGraphQueueHazardTests
    {
        struct PassData { }

        static void Add(RGBuilder graph, string name, RGBufferRef resource, bool async, bool write, bool live = true)
        {
            using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler(name)))
            {
                pass.EnableAsyncCompute(async);
                pass.EnablePassCulling(!live);
                if (write) pass.WriteBuffer(resource); else pass.ReadBuffer(resource);
                if (!live) pass.WriteBuffer(graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint))));
                pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands, RGObjectPool objects) => { });
            }
        }

        static string[] Compile(RGBuilder graph)
        {
            typeof(RGBuilder).GetMethod("CompilePass", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(graph, null);
            return graph.DescribeCompiledGraph().Split('\n').Skip(1).Where(line => line.Length > 0).ToArray();
        }

        [TestCase(false, false, true)] // WAR
        [TestCase(false, true, false)] // RAW
        [TestCase(false, true, true)] // WAW
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void OppositeQueue_WaitsForPriorHazard(bool firstAsync, bool firstWrite, bool secondWrite)
        {
            var graph = new RGBuilder("QueueHazard", null, true);
            try
            {
                var resource = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)));
                Add(graph, "First", resource, firstAsync, firstWrite);
                Add(graph, "Second", resource, !firstAsync, secondWrite);
                string[] rows = Compile(graph);
                Assert.AreEqual("0", rows[1].Split('|')[5]);
                Assert.AreEqual("True", rows[0].Split('|')[11]);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void Writer_WaitsForLatestOppositeReader_AcrossResources()
        {
            var graph = new RGBuilder("LatestReader", null, true);
            try
            {
                var a = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)));
                var b = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)));
                Add(graph, "ReadA", a, true, false);
                Add(graph, "ReadB", b, true, false);
                using (var pass = graph.AddComputePass<PassData>(new ProfilingSampler("WriteBoth")))
                {
                    pass.EnablePassCulling(false);
                    pass.WriteBuffer(b);
                    pass.WriteBuffer(a); // Resource enumeration must not replace a later fence with an earlier one.
                    pass.SetExecuteFunc((in PassData data, in RGComputeEncoder commands, RGObjectPool objects) => { });
                }
                Assert.AreEqual("1", Compile(graph)[2].Split('|')[5]);
            }
            finally { graph.Dispose(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SameQueue_DoesNotIntroduceCrossQueueFence(bool async)
        {
            var graph = new RGBuilder("SameQueue", null, true);
            try
            {
                var resource = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)));
                Add(graph, "Read", resource, async, false);
                Add(graph, "Write", resource, async, true);
                Assert.AreEqual("-1", Compile(graph)[1].Split('|')[5]);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void ConcurrentReaders_KeepBothQueueCompletionFencesForPhysicalReuse()
        {
            var graph = new RGBuilder("ConcurrentReaders", null, true);
            try
            {
                var resource = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)));
                Add(graph, "Produce", resource, false, true);
                Add(graph, "AsyncRead", resource, true, false);
                Add(graph, "GraphicsRead", resource, false, false);
                string[] rows = Compile(graph);
                Assert.AreEqual("-1", rows[2].Split('|')[5], "Read/read must remain concurrent.");
                Assert.AreEqual("True", rows[1].Split('|')[11]);
                Assert.AreEqual("True", rows[2].Split('|')[11]);
                StringAssert.Contains("Buffer:0", rows[2].Split('|')[10]);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void UnsupportedAsync_UsesGraphicsAndReportsActualQueue()
        {
            var graph = new RGBuilder("NoAsyncCapability", null, false);
            try
            {
                var resource = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)));
                Add(graph, "RequestedAsync", resource, true, true);
                Add(graph, "Consumer", resource, false, false);
                string[] rows = Compile(graph);
                Assert.AreEqual("Graphics", rows[0].Split('|')[3]);
                Assert.AreEqual("-1", rows[1].Split('|')[5]);
                Assert.AreEqual("False", rows[0].Split('|')[11]);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void CulledReader_DoesNotHoldWriter()
        {
            var graph = new RGBuilder("CulledReader", null, true);
            try
            {
                var resource = graph.CreateBuffer(new BufferDescriptor(1, sizeof(uint)));
                Add(graph, "UnusedRead", resource, true, false, false);
                Add(graph, "Write", resource, false, true);
                string[] rows = Compile(graph);
                Assert.AreEqual("True", rows[0].Split('|')[4]);
                Assert.AreEqual("-1", rows[1].Split('|')[5]);
            }
            finally { graph.Dispose(); }
        }

        [Test]
        public void RepeatedBufferImport_UsesOneHazardIdentity()
        {
            var buffer = new ComputeBuffer(1, sizeof(uint));
            try
            {
                var graph = new RGBuilder("BufferAliases", null, true);
                try
                {
                    var descriptor = new BufferDescriptor(1, sizeof(uint));
                    var a = graph.ImportBuffer(new FBufferRef(-1, descriptor, buffer));
                    var b = graph.ImportBuffer(new FBufferRef(-1, descriptor, buffer));
                    Assert.AreEqual(a.handle.index, b.handle.index);
                    Add(graph, "ReadAlias", a, true, false);
                    Add(graph, "OverwriteAlias", b, false, true);
                    Assert.AreEqual("0", Compile(graph)[1].Split('|')[5]);
                }
                finally { graph.Dispose(); }
            }
            finally { buffer.Release(); }
        }

        [Test]
        public void RepeatedTextureImport_PreservesBindingsAndRejectsConflictingView()
        {
            var texture = new RenderTexture(8, 8, 0);
            RTHandle first = RTHandles.Alloc(texture);
            RTHandle second = RTHandles.Alloc(texture);
            var factory = new RGResourceFactory();
            try
            {
                var descriptor = new TextureDescriptor(8, 8);
                var a = factory.ImportTexture(first, descriptor, Shader.PropertyToID("_FirstAlias"));
                var b = factory.ImportTexture(second, descriptor, Shader.PropertyToID("_SecondAlias"));
                Assert.AreEqual(a.handle.index, b.handle.index);
                var resource = (RGTexture)typeof(RGResourceFactory).GetMethod("GetTextureResource", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(factory, new object[] { a.handle });
                CollectionAssert.Contains(resource.additionalShaderProperties, Shader.PropertyToID("_SecondAlias"));
                descriptor.width = 7;
                Assert.Throws<InvalidOperationException>(() => factory.ImportTexture(second, descriptor, 0));
                Assert.Throws<InvalidOperationException>(() => factory.ImportBackbuffer(new RenderTargetIdentifier(texture), descriptor));
                factory.Clear();
                descriptor.width = 8;
                Assert.DoesNotThrow(() => factory.ImportTexture(first, descriptor, 0));
            }
            finally
            {
                factory.Dispose();
                first.Release();
                second.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
