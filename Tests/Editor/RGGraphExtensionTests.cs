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
            public int contributions, clears;
            public readonly List<(int pass, bool culled, int[] uses)> seen = new List<(int, bool, int[])>();

            public void Contribute(RGBuilder builder)
            {
                contributions++;
                for (int i = 0; i < builder.passCount; ++i)
                    seen.Add((i, builder.IsCulled(i), builder.GetPass(i).extensionUses.ToArray()));
            }

            public void Clear() => clears++;
        }

        [Test]
        public void Extension_SeesLivenessAndUses_AndClearsWithTheGraph()
        {
            var graph = new RGBuilder("GraphExtension");
            var extension = new RecordingExtension();
            graph.extension = extension;
            try
            {
                RGBufferHandle scratch = graph.CreateBuffer(new BufferDescriptor(4, 4) { name = "Unread" });
                using (var unread = graph.AddComputePass<EmptyPassData>(new UnityEngine.Rendering.ProfilingSampler("Unread")))
                {
                    unread.WriteBuffer(scratch);
                    unread.SetExecuteFunc((in EmptyPassData data, in RGComputeEncoder encoder) => { });
                }
                graph.GetPass(0).extensionUses.Add(3);
                using (var kept = graph.AddComputePass<EmptyPassData>(new UnityEngine.Rendering.ProfilingSampler("Kept")))
                {
                    kept.EnablePassCulling(false);
                    kept.SetExecuteFunc((in EmptyPassData data, in RGComputeEncoder encoder) => { });
                }
                graph.GetPass(1).extensionUses.Add(7);

                MethodInfo compile = typeof(RGBuilder).GetMethod("CompilePass", BindingFlags.Instance | BindingFlags.NonPublic);
                compile.Invoke(graph, null);

                Assert.AreEqual(1, extension.contributions);
                Assert.AreEqual(2, extension.seen.Count);
                Assert.IsTrue(extension.seen[0].culled);
                Assert.IsFalse(extension.seen[1].culled);
                CollectionAssert.AreEqual(new[] { 7 }, extension.seen[1].uses);

                graph.ClearRecordedGraph();
                Assert.AreEqual(1, extension.clears);
                Assert.AreEqual(0, graph.passCount);
            }
            finally
            {
                graph.extension = null;
                graph.Dispose();
            }
        }
    }
}
