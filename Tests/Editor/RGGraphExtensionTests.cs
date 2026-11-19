using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using InfinityTech.Rendering.GPUResource;

namespace InfinityTech.Rendering.RenderGraph.Tests
{
    public class RGGraphExtensionTests
    {
        struct EmptyPassData { }

        sealed class RecordingExtension : IRGGraphExtension
        {
            public int contributions, clears, disposals;
            public readonly List<(int pass, bool culled, int[] uses)> seen = new List<(int, bool, int[])>();

            public void Contribute(RGBuilder builder)
            {
                contributions++;
                for (int i = 0; i < builder.passCount; ++i)
                    seen.Add((i, builder.IsCulled(i), builder.GetPass(i).extensionUses.ToArray()));
            }

            public void Clear() => clears++;

            public void Dispose() => disposals++;
        }

        [Test]
        public void Extension_SeesLivenessAndUses_AndClearsWithTheGraph()
        {
            var graph = new RGBuilder("GraphExtension");
            var extension = new RecordingExtension();
            graph.extension = extension;
            try
            {
                RGBufferHandle scratchHandle = graph.CreateBuffer(new BufferDescriptor(4, 4) { name = "Unread" });
                {
                    RGComputePassHandle<EmptyPassData> passHandle = graph.AddComputePass<EmptyPassData>(new UnityEngine.Rendering.ProfilingSampler("Unread"));
                    passHandle.WriteBuffer(scratchHandle);
                    passHandle.SetExecuteFunc((in EmptyPassData data, in RGComputeEncoder encoder) => { });
                }
                graph.GetPass(0).extensionUses.Add(3);
                {
                    RGComputePassHandle<EmptyPassData> passHandle = graph.AddComputePass<EmptyPassData>(new UnityEngine.Rendering.ProfilingSampler("Kept"));
                    passHandle.EnablePassCulling(false);
                    passHandle.SetExecuteFunc((in EmptyPassData data, in RGComputeEncoder encoder) => { });
                }
                graph.GetPass(1).extensionUses.Add(7);

                MethodInfo compile = typeof(RGBuilder).GetMethod("CompilePass", BindingFlags.Instance | BindingFlags.NonPublic);
                compile.Invoke(graph, null);

                Assert.AreEqual(1, extension.contributions);
                Assert.AreEqual(2, extension.seen.Count);
                Assert.IsTrue(extension.seen[0].culled);
                Assert.IsFalse(extension.seen[1].culled);
                CollectionAssert.AreEqual(new[] { 7 }, extension.seen[1].uses);
                Assert.AreEqual("Unread", graph.GetPass(0).name);
                Assert.AreEqual("Kept", graph.GetPass(1).name);

                graph.ClearRecordedGraph();
                Assert.AreEqual(1, extension.clears);
                Assert.AreEqual(0, graph.passCount);
                Assert.AreEqual(0, extension.disposals, "Dropping a recorded graph keeps the extension.");
            }
            finally
            {
                graph.Dispose();
            }
            Assert.AreEqual(2, extension.clears, "Dispose drops the recorded graph first.");
            Assert.AreEqual(1, extension.disposals, "The builder owns its extension and disposes it once.");
        }
    }
}
